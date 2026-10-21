class_name GeoRegionData
extends RefCounted
## One production area's constraint layers for the region level (geo stages
## 2-3 plan Track D): a static, lazy cache, built on the first drill into an
## area and read from memory on every later one.
##
## For an area it keeps, in the area base map's pixels (basemap.json poNN
## bounds and res):
## - locality_areas: the registered site polygons (id);
## - mooring_areas: NYTEK installation and mooring polygons (id, areatype);
## - fairways: fairways.regional's centre lines with their exclusion
##   half-width buffer_m, in pixels;
## - exclusions: exclusions.regional's salmon fjords, protected areas and
##   coral closures (kind, name);
## - constraint_points: river mouths and coral reefs (kind, name) with the
##   radius and hardness of their kind (constraint_points meta.kinds).
## Polygons are projected once (GeoProjection.utm33, 64-bit) and
## pre-triangulated (Geometry2D.triangulate_polygon). A ring that does not
## triangulate keeps its outline and no fill: no engine error is logged.
##
## A feature belongs to the area when its projected extent meets the base
## map's bounds, the area's bbox_25833 padded by about 15 km: what the view
## shows (deviation from the plan's bbox_25833, RUN_LOG §7). The five files
## are parsed once per process by GeoData; a cheap lon/lat prefilter (the
## base map's bounds taken back to lon/lat, padded) keeps the projection to
## the features that can reach the map.
##
## Main thread only, like GeoData. Display only: nothing feeds the sim.

const LAYERS := {
	"locality_areas": "locality_areas.geojson",
	"mooring_areas": "mooring_areas.geojson",
	"fairways": "fairways.regional.geojson",
	"exclusions": "exclusions.regional.geojson",
	"constraint_points": "constraint_points.geojson",
}
const LAYER_ORDER: Array[String] = ["exclusions", "locality_areas", "mooring_areas", "fairways", "constraint_points"]
## The lon/lat prefilter's padding, degrees (the inverse projection is exact
## to well under a metre; this only absorbs edge sampling).
const PREFILTER_PAD_DEG := 0.05
## Samples per edge when the base map's bounds are taken back to lon/lat.
const EDGE_SAMPLES := 12

## Areas built since the process started (a repeat drill builds nothing).
static var build_count: int = 0
## Polygon parts that kept only their outline since the process started.
static var fallback_count: int = 0

static var _areas: Dictionary = {}       # po -> frozen area
static var _ll_boxes: Dictionary = {}    # layer -> PackedFloat64Array, 4 per feature


## The area's layers: {po, map_size, res, bounds, layers {name: Array},
## counts {name: int}, fallbacks, build_ms}. {} for an area outside 1..13.
## A polygon feature: {id, kind, name, parts: Array[{ring: PackedVector2Array,
## holes: Array[PackedVector2Array], tris: PackedInt32Array}]}; tris is empty
## for an outline-only part. A fairway: {kind, name, buffer_px, line}. A point:
## {kind, name, px, radius_px, hard}.
static func area(po: int) -> Dictionary:
	if _areas.has(po):
		return _areas[po]
	var t := GeoRegionMath.transform(po)
	if t.is_empty():
		return {}
	var started := Time.get_ticks_usec()
	var map_size: Vector2 = t["map_size"]
	var bounds: Array = t["map_bounds"]
	var res: float = t["map_res"]
	var box := _lonlat_box(bounds)
	var layers := {}
	var counts := {}
	var fallbacks_before := fallback_count
	for layer: String in LAYER_ORDER:
		var rows: Array = []
		var features := _features(layer)
		var boxes := _boxes(layer, features)
		for i in features.size():
			if not _overlaps(boxes, i, box):
				continue
			var row := _project(layer, features[i], bounds, res, map_size)
			if not row.is_empty():
				rows.append(row)
		rows.make_read_only()
		layers[layer] = rows
		counts[layer] = rows.size()
	var out := {
		"po": po,
		"map_size": map_size,
		"res": res,
		"bounds": bounds,
		"layers": layers,
		"counts": counts,
		"fallbacks": fallback_count - fallbacks_before,
		"build_ms": (Time.get_ticks_usec() - started) / 1000.0,
	}
	out.make_read_only()
	_areas[po] = out
	build_count += 1
	return out


## True when the area's layers are already in memory.
static func is_cached(po: int) -> bool:
	return _areas.has(po)


## The fill triangles of a ring (no closing point), or empty when it does not
## triangulate (the outline fallback).
static func fill_indices(ring: PackedVector2Array) -> PackedInt32Array:
	if ring.size() < 3:
		return PackedInt32Array()
	return Geometry2D.triangulate_polygon(ring)


## EPSG:25833 metres -> [lon, lat] degrees: the Krüger series' inverse to n^4
## (GRS80, k0 0.9996, 15°E, false easting 500 000 m), GeoProjection.utm33's
## twin. Display only: it frames the prefilter.
static func utm_to_lonlat(x: float, y: float) -> PackedFloat64Array:
	var n := GeoProjection.FLATTENING / (2.0 - GeoProjection.FLATTENING)
	var n2 := n * n
	var n3 := n2 * n
	var n4 := n3 * n
	var big_a := GeoProjection.SEMI_MAJOR_M / (1.0 + n) * (1.0 + n2 / 4.0 + n4 / 64.0)
	var beta := [
		n / 2.0 - 2.0 * n2 / 3.0 + 37.0 * n3 / 96.0 - n4 / 360.0,
		n2 / 48.0 + n3 / 15.0 - 437.0 * n4 / 1440.0,
		17.0 * n3 / 480.0 - 37.0 * n4 / 840.0,
		4397.0 * n4 / 161280.0]
	var delta := [
		2.0 * n - 2.0 * n2 / 3.0 - 2.0 * n3 + 116.0 * n4 / 45.0,
		7.0 * n2 / 3.0 - 8.0 * n3 / 5.0 - 227.0 * n4 / 45.0,
		56.0 * n3 / 15.0 - 136.0 * n4 / 35.0,
		4279.0 * n4 / 630.0]
	var xi := y / (GeoProjection.SCALE_K0 * big_a)
	var eta := (x - GeoProjection.FALSE_EASTING_M) / (GeoProjection.SCALE_K0 * big_a)
	var xi_p := xi
	var eta_p := eta
	for j in range(1, 5):
		var b: float = beta[j - 1]
		xi_p -= b * sin(2.0 * j * xi) * _cosh(2.0 * j * eta)
		eta_p -= b * cos(2.0 * j * xi) * _sinh(2.0 * j * eta)
	var chi := asin(sin(xi_p) / _cosh(eta_p))
	var phi := chi
	for j in range(1, 5):
		phi += float(delta[j - 1]) * sin(2.0 * j * chi)
	var lam := atan2(_sinh(eta_p), cos(xi_p))
	return PackedFloat64Array([GeoProjection.CENTRAL_MERIDIAN_DEG + rad_to_deg(lam), rad_to_deg(phi)])


## Tests only: forget every built area and counter (GeoData keeps its files).
static func reset_cache() -> void:
	_areas = {}
	_ll_boxes = {}
	build_count = 0
	fallback_count = 0


# ── Internals ─────────────────────────────────────────────────────────────────

static func _features(layer: String) -> Array:
	var data: Variant = GeoData.read_json(LAYERS[layer])
	var features: Variant = data.get("features") if data is Dictionary else null
	return features if features is Array else []


static func _kinds_meta() -> Dictionary:
	var data: Variant = GeoData.read_json(LAYERS["constraint_points"])
	var meta: Variant = data.get("meta") if data is Dictionary else null
	var kinds: Variant = meta.get("kinds") if meta is Dictionary else null
	return kinds if kinds is Dictionary else {}


## Each feature's lon/lat box [min lon, min lat, max lon, max lat], once per
## process per layer.
static func _boxes(layer: String, features: Array) -> PackedFloat64Array:
	if _ll_boxes.has(layer):
		return _ll_boxes[layer]
	var out := PackedFloat64Array()
	out.resize(features.size() * 4)
	for i in features.size():
		var geometry: Variant = (features[i] as Dictionary).get("geometry")
		var rings := _outer_rings(geometry if geometry is Dictionary else {})
		var x0 := INF
		var y0 := INF
		var x1 := -INF
		var y1 := -INF
		for ring: Array in rings:
			for p: Array in ring:
				var lon: float = p[0]
				var lat: float = p[1]
				x0 = minf(x0, lon)
				y0 = minf(y0, lat)
				x1 = maxf(x1, lon)
				y1 = maxf(y1, lat)
		out[i * 4] = x0
		out[i * 4 + 1] = y0
		out[i * 4 + 2] = x1
		out[i * 4 + 3] = y1
	_ll_boxes[layer] = out
	return out


## The point lists that bound a geometry: a polygon's outer ring (its holes
## lie inside it), every part's outer ring, a line, or the one point.
static func _outer_rings(geometry: Dictionary) -> Array:
	var coords: Variant = geometry.get("coordinates", [])
	if not coords is Array or (coords as Array).is_empty():
		return []
	match String(geometry.get("type", "")):
		"Point":
			return [[coords]]
		"LineString":
			return [coords]
		"Polygon":
			return [coords[0]]
		"MultiPolygon":
			var out: Array = []
			for poly: Variant in coords:
				if poly is Array and not (poly as Array).is_empty():
					out.append(poly[0])
			return out
	return []


## The base map's bounds as a lon/lat box, padded.
static func _lonlat_box(bounds: Array) -> PackedFloat64Array:
	var x0 := float(bounds[0])
	var y0 := float(bounds[1])
	var x1 := float(bounds[2])
	var y1 := float(bounds[3])
	var b := [INF, INF, -INF, -INF]
	for i in EDGE_SAMPLES + 1:
		var f := float(i) / EDGE_SAMPLES
		for p in [[lerpf(x0, x1, f), y0], [lerpf(x0, x1, f), y1], [x0, lerpf(y0, y1, f)], [x1, lerpf(y0, y1, f)]]:
			var ll := utm_to_lonlat(p[0], p[1])
			b[0] = minf(b[0], ll[0])
			b[1] = minf(b[1], ll[1])
			b[2] = maxf(b[2], ll[0])
			b[3] = maxf(b[3], ll[1])
	return PackedFloat64Array([b[0] - PREFILTER_PAD_DEG, b[1] - PREFILTER_PAD_DEG,
		b[2] + PREFILTER_PAD_DEG, b[3] + PREFILTER_PAD_DEG])


static func _overlaps(boxes: PackedFloat64Array, i: int, box: PackedFloat64Array) -> bool:
	return boxes[i * 4] <= box[2] and boxes[i * 4 + 2] >= box[0] \
		and boxes[i * 4 + 1] <= box[3] and boxes[i * 4 + 3] >= box[1]


## One feature in map pixels, or {} when its exact extent misses the map.
static func _project(layer: String, feature: Dictionary, bounds: Array, res: float, map_size: Vector2) -> Dictionary:
	var props: Dictionary = feature.get("properties", {})
	var geometry: Dictionary = feature.get("geometry", {})
	var gtype := String(geometry.get("type", ""))
	var coords: Variant = geometry.get("coordinates", [])
	var view := Rect2(Vector2.ZERO, map_size)
	match gtype:
		"Point":
			if layer != "constraint_points":
				return {}
			var px := GeoProjection.to_px(float(props.get("x", 0.0)), float(props.get("y", 0.0)), bounds, res)
			var kind := _text(props.get("kind"))
			var meta: Dictionary = _kinds_meta().get(kind, {})
			var radius := float(meta.get("radius_m", 0.0)) / res
			if not view.grow(radius).has_point(px):
				return {}
			return {"kind": kind, "name": _text(props.get("name")), "px": px,
				"radius_px": radius, "hard": bool(meta.get("hard", false))}
		"LineString":
			var line := _ring(coords, bounds, res)
			var buffer_px := float(props.get("buffer_m", 0.0)) / res
			if line.size() < 2 or not _rect_of(line).grow(buffer_px).intersects(view):
				return {}
			return {"kind": _text(props.get("kind")), "name": _text(props.get("name")),
				"buffer_px": buffer_px, "line": line}
		"Polygon", "MultiPolygon":
			var polys: Array = [coords] if gtype == "Polygon" else coords
			var parts: Array = []
			var extent := Rect2()
			for poly: Variant in polys:
				if not poly is Array or (poly as Array).is_empty():
					continue
				var ring := _open_ring(_ring(poly[0], bounds, res))
				if ring.size() < 3:
					continue
				var holes: Array[PackedVector2Array] = []
				for h in range(1, (poly as Array).size()):
					holes.append(_open_ring(_ring(poly[h], bounds, res)))
				var r := _rect_of(ring)
				extent = r if parts.is_empty() else extent.merge(r)
				var tris := fill_indices(ring)
				if tris.is_empty():
					fallback_count += 1
				parts.append({"ring": ring, "holes": holes, "tris": tris})
			if parts.is_empty() or not extent.intersects(view):
				return {}
			return {"id": int(props.get("id", 0)), "kind": _text(props.get("kind", props.get("areatype", ""))),
				"name": _text(props.get("name")), "parts": parts}
	return {}


static func _ring(points: Variant, bounds: Array, res: float) -> PackedVector2Array:
	var out := PackedVector2Array()
	if not points is Array:
		return out
	out.resize((points as Array).size())
	var i := 0
	for p: Variant in points:
		var xy := GeoProjection.utm33(float(p[0]), float(p[1]))
		out[i] = GeoProjection.to_px(xy[0], xy[1], bounds, res)
		i += 1
	return out


## A GeoJSON ring is closed (last == first); triangulation wants it open.
static func _open_ring(ring: PackedVector2Array) -> PackedVector2Array:
	if ring.size() > 1 and ring[0].is_equal_approx(ring[ring.size() - 1]):
		ring.resize(ring.size() - 1)
	return ring


static func _rect_of(points: PackedVector2Array) -> Rect2:
	var r := Rect2(points[0], Vector2.ZERO)
	for p in points:
		r = r.expand(p)
	return r


## A GeoJSON property as text: "" for a missing or null value (a fairway
## or river may have no name).
static func _text(v: Variant) -> String:
	return "" if v == null else str(v)


static func _sinh(v: float) -> float:
	return 0.5 * (exp(v) - exp(-v))


static func _cosh(v: float) -> float:
	return 0.5 * (exp(v) + exp(-v))
