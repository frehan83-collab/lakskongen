class_name GeoOverlayLayer
extends Control
## The national map's planning overlays (geo stages 2-3 plan, Track C):
## - the 19 Havbruk til havs areas (offshore_areas.national) in buoy yellow,
##   dashed by stage: dotted when recommended (2019), longer dashes as an
##   area moves on (2022, 2024), solid with a light fill once it can be
##   announced (2026);
## - the 30 national salmon fjords as a soft red hatch (triangulated, with a
##   procedural repeating hatch), or as an outline when a fjord does not
##   triangulate at this scale, so no engine error is ever logged;
## - the candidates: the 156 open-pen sites as one salmon star per area
##   (12 each, at the area's best-ranked site) and the 33 offshore sites as
##   yellow hexagons, the one hav_1 is bound to ringed.
##
## Two instances share the code: part "areas" draws the fjords and the
## offshore areas under the site icons, part "candidates" the stars and the
## hexagons above them, so no cluster badge hides a candidate.
## One _draw each, recorded again only on a view change or a layer toggle,
## never on a tick (redraw_count). Polygons are projected once per process.

const STAGES: Array[String] = ["tilradning_2019", "anbefaling_2022", "konsekvensvurdering_2024", "kan_utlyses_2026"]
## Per stage: [dash px, gap px (0: solid), width px, alpha].
const STAGE_STROKES: Array = [[2.0, 4.0, 1.3, 0.6], [5.0, 4.0, 1.5, 0.78], [9.0, 4.0, 1.7, 0.9], [1.0, 0.0, 2.0, 1.0]]
const HATCH_PX := 7.0
const STAR_HIT_PX := 8.0
const HEX_HIT_PX := 7.0
const HAV_PLOT := "hav_1"

## "areas" (fjords, offshore areas) or "candidates" (stars, hexagons).
var part := "areas"
var redraw_count: int = 0
var show_candidates := true
var show_offshore := true
var show_fjords := true
## How the last recording drew the fjords (tests: filled + outlined == 30).
var fjords_filled: int = 0
var fjords_outlined: int = 0
var offshore_drawn: int = 0

## National-image pixel polygons, once per process: [{name, stage, ring}].
static var _offshore_px: Array = []
static var _fjords_px: Array = []
static var _hatch: ImageTexture = null

var _origin := Vector2.ZERO
var _scale := 1.0
var _bounds: Array = []
var _res := 1.0
var _offshore_local: Array = []
var _fjords_local: Array = []
var _stars: Array = []
var _hexes: Array = []
var _offshore_rows: Array = []


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	texture_repeat = CanvasItem.TEXTURE_REPEAT_ENABLED


## The offshore candidates as GeoBridgeAdapter.offshore_candidates returns them.
func set_offshore_candidates(rows: Array) -> void:
	_offshore_rows = rows
	_rebuild()


## Map national-image pixels to this layer: local = origin + px * scale.
func set_view(origin: Vector2, px_scale: float, bounds_25833: Array, res_m: float) -> void:
	_origin = origin
	_scale = px_scale
	_bounds = bounds_25833
	_res = res_m
	_rebuild()


## Show or hide one overlay ("candidates", "offshore", "fjords").
func set_layer(layer: String, on: bool) -> void:
	match layer:
		"candidates":
			show_candidates = on
		"offshore":
			show_offshore = on
		"fjords":
			show_fjords = on
	queue_redraw()


## One star per area: [{po, pos, count, best: candidate dict}].
func star_groups() -> Array:
	return _stars


## Every offshore candidate: [{row, pos, bound}].
func hexagons() -> Array:
	return _hexes


## The candidate, offshore area or salmon fjord under a layer point:
## {kind: "candidates"|"hexagon"|"offshore_area"|"fjord", ...}, or {}.
## Candidates: the nearest star or hexagon within its hit radius; on equal
## distance the one drawn later (above) wins, and the ringed hav_1 hexagon is
## drawn last of all. "distance" is the pointer's distance to the item.
func item_at(p: Vector2) -> Dictionary:
	if show_candidates and part == "candidates":
		var best: Dictionary = {}
		var best_d := INF
		for s: Dictionary in _stars:
			var d := (s["pos"] as Vector2).distance_to(p)
			if d <= STAR_HIT_PX and d <= best_d:
				best_d = d
				best = {"kind": "candidates", "po": s["po"], "count": s["count"], "best": s["best"], "pos": s["pos"],
					"distance": d}
		for h: Dictionary in _hexes:
			var d := (h["pos"] as Vector2).distance_to(p)
			if d <= HEX_HIT_PX and d <= best_d:
				best_d = d
				best = {"kind": "hexagon", "row": h["row"], "bound": h["bound"], "pos": h["pos"], "distance": d}
		if not best.is_empty():
			return best
	if show_offshore and part == "areas":
		for i in range(_offshore_local.size() - 1, -1, -1):
			var o: Dictionary = _offshore_local[i]
			if Geometry2D.is_point_in_polygon(p, o["ring"]):
				return {"kind": "offshore_area", "name": o["name"], "stage": o["stage"]}
	if show_fjords and part == "areas":
		for f: Dictionary in _fjords_local:
			if Geometry2D.is_point_in_polygon(p, f["ring"]):
				return {"kind": "fjord", "name": f["name"]}
	return {}


func _rebuild() -> void:
	if _bounds.size() != 4:
		return
	_project_once()
	_offshore_local = _to_local(_offshore_px) if part == "areas" else []
	_fjords_local = _to_local(_fjords_px) if part == "areas" else []
	_stars.clear()
	_hexes.clear()
	if part != "candidates":
		queue_redraw()
		return
	var best_by_po := {}
	var count_by_po := {}
	for s: Dictionary in GeoData.candidates():
		if str(s.get("type", "")) != "open_pen":
			continue
		var po := int(s["po"])
		count_by_po[po] = int(count_by_po.get(po, 0)) + 1
		if not best_by_po.has(po) or int(s["rank"]) < int(best_by_po[po]["rank"]):
			best_by_po[po] = s
	var pos_keys := best_by_po.keys()
	pos_keys.sort()
	for po: int in pos_keys:
		var best: Dictionary = best_by_po[po]
		_stars.append({"po": po, "count": count_by_po[po], "best": best,
			"pos": _local_of(float(best["x"]), float(best["y"]))})
	_hexes.clear()
	var ringed: Array = []
	for row: Dictionary in _offshore_rows:
		var h := {"row": row, "pos": _local_of(float(row["x"]), float(row["y"])),
			"bound": str(row.get("bound_plot_id", "")) == HAV_PLOT}
		(ringed if h["bound"] else _hexes).append(h)
	# The ringed one last: drawn above its neighbours, and first to answer.
	_hexes.append_array(ringed)
	queue_redraw()


func _local_of(x: float, y: float) -> Vector2:
	return _origin + GeoProjection.to_px(x, y, _bounds, _res) * _scale


func _to_local(polys: Array) -> Array:
	var out: Array = []
	for o: Dictionary in polys:
		var ring := PackedVector2Array()
		for q in (o["ring"] as PackedVector2Array):
			ring.append(_origin + q * _scale)
		out.append({"name": o["name"], "stage": o.get("stage", ""), "ring": ring})
	return out


## Project the national offshore areas and salmon fjords to national-image
## pixels, once per process (lon/lat -> UTM 33, 64-bit -> pixels).
func _project_once() -> void:
	if not _offshore_px.is_empty() and not _fjords_px.is_empty():
		return
	var features: Array = GeoData.offshore_areas("national").duplicate()
	features.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		return STAGES.find(str(a["properties"]["stage"])) < STAGES.find(str(b["properties"]["stage"])))
	_offshore_px = []
	for f: Dictionary in features:
		_offshore_px.append({"name": str(f["properties"].get("name", "")), "stage": str(f["properties"]["stage"]),
			"ring": _ring_px(f)})
	_fjords_px = []
	for f: Dictionary in GeoData.salmon_fjords():
		_fjords_px.append({"name": str(f["properties"].get("name", "")), "ring": _ring_px(f)})


func _ring_px(feature: Dictionary) -> PackedVector2Array:
	var geom: Dictionary = feature["geometry"]
	var coords: Array = geom["coordinates"]
	var outer: Array = coords[0] if str(geom["type"]) == "Polygon" else coords[0][0]
	var ring := PackedVector2Array()
	for ll: Array in outer:
		var xy := GeoProjection.utm33(float(ll[0]), float(ll[1]))
		var px := GeoProjection.to_px(xy[0], xy[1], _bounds, _res)
		if ring.is_empty() or ring[ring.size() - 1].distance_to(px) > 0.01:
			ring.append(px)
	if ring.size() > 1 and ring[0].distance_to(ring[ring.size() - 1]) <= 0.01:
		ring.remove_at(ring.size() - 1)
	return ring


func _draw() -> void:
	redraw_count += 1
	fjords_filled = 0
	fjords_outlined = 0
	offshore_drawn = 0
	if part == "areas":
		if show_fjords:
			_draw_fjords()
		if show_offshore:
			_draw_offshore()
	elif show_candidates:
		_draw_candidates()


func _draw_fjords() -> void:
	var red := MapPalette.fjord_red()
	var hatch := _hatch_texture()
	for f: Dictionary in _fjords_local:
		var ring: PackedVector2Array = f["ring"]
		if ring.size() < 3:
			fjords_outlined += 1
			continue
		if fill_or_outline(self, ring, Color(red.lightened(0.25), 0.75), Color(red.lightened(0.2), 0.85), hatch):
			fjords_filled += 1
		else:
			fjords_outlined += 1


## A hatched area, or its outline alone when it does not triangulate:
## draw_colored_polygon logs an engine error on a polygon it cannot
## triangulate, so the triangulation is tried first. True when filled.
static func fill_or_outline(canvas: CanvasItem, ring: PackedVector2Array, fill: Color, line: Color,
		hatch: Texture2D) -> bool:
	var filled := ring.size() >= 3 and not Geometry2D.triangulate_polygon(ring).is_empty()
	if filled:
		var uvs := PackedVector2Array()
		for q in ring:
			uvs.append(q / HATCH_PX)
		canvas.draw_colored_polygon(ring, fill, uvs, hatch)
	if ring.size() >= 2:
		canvas.draw_polyline(_closed(ring), line, 1.0, true)
	return filled


func _draw_offshore() -> void:
	var buoy := MapPalette.buoy()
	var deep := MapPalette.deep_water()
	for o: Dictionary in _offshore_local:
		var ring: PackedVector2Array = o["ring"]
		if ring.size() < 3:
			continue
		var stroke: Array = STAGE_STROKES[maxi(STAGES.find(str(o["stage"])), 0)]
		if float(stroke[1]) == 0.0:
			if not Geometry2D.triangulate_polygon(ring).is_empty():
				draw_colored_polygon(ring, Color(buoy, 0.16))
			draw_polyline(_closed(ring), Color(deep, 0.6), float(stroke[2]) + 2.0, true)
			draw_polyline(_closed(ring), Color(buoy, float(stroke[3])), float(stroke[2]), true)
		else:
			var dashes := _dashed(_closed(ring), float(stroke[0]), float(stroke[1]))
			if not dashes.is_empty():
				draw_multiline(dashes, Color(buoy, float(stroke[3])), float(stroke[2]))
		offshore_drawn += 1


func _draw_candidates() -> void:
	var deep := MapPalette.deep_water()
	# One star per area (its twelve sites; the tooltip and the key say so).
	for s: Dictionary in _stars:
		var p: Vector2 = s["pos"]
		draw_colored_polygon(_star(p, 8.0, 3.6), Color(deep, 0.85))
		draw_colored_polygon(_star(p, 6.4, 2.7), MapPalette.salmon())
	for h: Dictionary in _hexes:
		var p: Vector2 = h["pos"]
		if h["bound"]:
			draw_arc(p, 10.5, 0.0, TAU, 32, Color(deep, 0.8), 4.0, true)
			draw_arc(p, 10.5, 0.0, TAU, 32, MapPalette.snow(), 2.0, true)
		draw_colored_polygon(GeoSiteLayer._ngon(p, 6.2, 6), Color(deep, 0.85))
		draw_colored_polygon(GeoSiteLayer._ngon(p, 4.8, 6), MapPalette.buoy())
		if h["bound"]:
			draw_circle(p, 1.8, deep)


static func _star(c: Vector2, outer: float, inner: float) -> PackedVector2Array:
	var out := PackedVector2Array()
	for k in 10:
		var a := TAU * float(k) / 10.0 - PI * 0.5
		out.append(c + Vector2(cos(a), sin(a)) * (outer if k % 2 == 0 else inner))
	return out


static func _closed(poly: PackedVector2Array) -> PackedVector2Array:
	var out := poly.duplicate()
	out.append(poly[0])
	return out


## Segment pairs of a dashed polyline (for draw_multiline).
static func _dashed(line: PackedVector2Array, dash: float, gap: float) -> PackedVector2Array:
	var out := PackedVector2Array()
	var on := true
	var left := dash
	for i in range(line.size() - 1):
		var a := line[i]
		var b := line[i + 1]
		var seg := a.distance_to(b)
		var t := 0.0
		while seg - t > 0.0001:
			var step := minf(left, seg - t)
			var p0 := a.lerp(b, t / seg)
			var p1 := a.lerp(b, (t + step) / seg)
			if on:
				out.append(p0)
				out.append(p1)
			t += step
			left -= step
			if left <= 0.0001:
				on = not on
				left = dash if on else gap
	return out


## A repeating diagonal hatch, white on clear (tinted when drawn), made once.
static func _hatch_texture() -> ImageTexture:
	if _hatch != null:
		return _hatch
	var n := int(HATCH_PX)
	var img := Image.create(n, n, false, Image.FORMAT_RGBA8)
	for y in n:
		for x in n:
			var d := (x + y) % n
			img.set_pixel(x, y, Color(1, 1, 1, 0.85 if d == 0 else (0.35 if d == 1 or d == n - 1 else 0.0)))
	_hatch = ImageTexture.create_from_image(img)
	return _hatch
