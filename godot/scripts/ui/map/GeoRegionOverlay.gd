class_name GeoRegionOverlay
extends Control
## The region level's constraint layers (geo stages 2-3 plan Track D), drawn
## over the area's base map from GeoRegionData: salmon fjords, protected
## areas and coral closures, the registered site and mooring polygons, the
## fairways with their exclusion buffers, and the river mouths and coral
## reefs with their radii (solid when the rule is hard, dashed when soft).
##
## Everything is recorded in one _draw, in base-map pixels under one
## transform (the map's KEEP_ASPECT_CENTERED fit), and re-recorded only on a
## resize, a new area or a layer toggle, never on a tick: redraw_count counts
## the recordings. Fills are pre-merged triangle arrays (one call per style),
## outlines merged line lists, so a PO costs a few dozen draw calls. A ring
## GeoRegionData could not triangulate draws its outline only. Each
## recording counts what it emitted (drawn, drawn_kinds, drawn_triangles,
## drawn_segments), so the checks compare the drawing itself with the data.

## The groups a layer chip toggles.
const GROUP_OF := {
	"exclusions": "restrictions",
	"fairways": "restrictions",
	"constraint_points": "restrictions",
	"locality_areas": "sites",
	"mooring_areas": "sites",
}

const STYLE := {
	"salmon_fjord": {"fill": Color(0.851, 0.345, 0.231, 0.16), "line": Color(0.95, 0.47, 0.36, 0.85)},
	"protected_area": {"fill": Color(0.561, 0.604, 0.353, 0.14), "line": Color(0.70, 0.76, 0.46, 0.65)},
	"coral_closure": {"fill": Color(0.72, 0.55, 0.85, 0.14), "line": Color(0.80, 0.66, 0.92, 0.70)},
	"locality_areas": {"fill": Color(0.957, 0.969, 0.980, 0.22), "line": Color(0.957, 0.969, 0.980, 0.80)},
	"mooring_areas": {"fill": Color(0, 0, 0, 0), "line": Color(0.910, 0.725, 0.165, 0.55)},
	"hovedled": {"band": Color(0.55, 0.78, 0.85, 0.12), "line": Color(0.66, 0.85, 0.91, 0.55)},
	"biled": {"band": Color(0.55, 0.78, 0.85, 0.06), "line": Color(0.66, 0.85, 0.91, 0.28)},
	"national_salmon_river": {"fill": Color(0.851, 0.345, 0.231, 0.07), "line": Color(0.95, 0.47, 0.36, 0.90)},
	"anadromous_river": {"fill": Color(0, 0, 0, 0), "line": Color(0.66, 0.85, 0.91, 0.55)},
	"coral_reef": {"fill": Color(0.80, 0.66, 0.92, 0.85), "line": Color(0.80, 0.66, 0.92, 0.55)},
}
const DASH_SEGMENTS := 28
const REEF_DOT_PX := 1.6

## How many times the layers were recorded.
var redraw_count: int = 0
## Features the last recording emitted, per layer (GeoRegionData's names):
## a polygon feature once its style's triangles or outlines went out, a
## fairway with its band and line, a point with its ring.
var drawn: Dictionary = {}
## The same per style (salmon_fjord, protected_area, coral_closure,
## locality_areas, mooring_areas) and per fairway and point kind.
var drawn_kinds: Dictionary = {}
## Triangles and outline segments the last recording emitted, per style.
var drawn_triangles: Dictionary = {}
var drawn_segments: Dictionary = {}
## Polygon parts the last recording drew as an outline only.
var outline_only: int = 0

var _area: Dictionary = {}
var _hidden_groups: Dictionary = {}
var _fills: Dictionary = {}     # style -> {points, indices, colors}
var _outlines: Dictionary = {}  # style -> PackedVector2Array (segment pairs)
var _style_rows: Dictionary = {}  # style -> features with a fill or an outline
var _outline_parts: Dictionary = {}  # layer -> outline-only parts


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	resized.connect(queue_redraw)


## Show an area's layers (GeoRegionData.area(po)); {} clears.
func set_area(area: Dictionary) -> void:
	_area = area
	_build_batches()
	queue_redraw()


func area_po() -> int:
	return int(_area.get("po", 0))


## Show or hide a chip's group ("restrictions", "sites").
func set_group_visible(group: String, on: bool) -> void:
	if on == not _hidden_groups.has(group):
		return
	if on:
		_hidden_groups.erase(group)
	else:
		_hidden_groups[group] = true
	queue_redraw()


func group_visible(group: String) -> bool:
	return not _hidden_groups.has(group)


## The base map's drawn rect in this control (KEEP_ASPECT_CENTERED).
func map_rect() -> Rect2:
	return GeoRegionView.fit_rect(_area.get("map_size", Vector2.ONE), size)


func _draw() -> void:
	redraw_count += 1
	drawn = {}
	drawn_kinds = {}
	drawn_triangles = {}
	drawn_segments = {}
	outline_only = 0
	if _area.is_empty():
		return
	var rect := map_rect()
	var s := rect.size.x / float((_area["map_size"] as Vector2).x)
	draw_set_transform(rect.position, 0.0, Vector2(s, s))
	var ci := get_canvas_item()
	var layers: Dictionary = _area["layers"]
	for layer: String in GeoRegionData.LAYER_ORDER:
		if _hidden_groups.has(GROUP_OF[layer]):
			continue
		var rows: Array = layers.get(layer, [])
		var emitted := 0
		match layer:
			"exclusions", "locality_areas", "mooring_areas":
				for style: String in _styles_of(layer):
					var out := false
					var fill: Dictionary = _fills.get(style, {})
					if not fill.is_empty() and not (fill["indices"] as PackedInt32Array).is_empty():
						RenderingServer.canvas_item_add_triangle_array(ci, fill["indices"], fill["points"], fill["colors"])
						drawn_triangles[style] = (fill["indices"] as PackedInt32Array).size() / 3
						out = true
					var lines: PackedVector2Array = _outlines.get(style, PackedVector2Array())
					if lines.size() >= 2:
						draw_multiline(lines, STYLE[style]["line"], -1.0)
						drawn_segments[style] = lines.size() / 2
						out = true
					if out:
						emitted += int(_style_rows.get(style, 0))
						drawn_kinds[style] = int(_style_rows.get(style, 0))
				outline_only += int(_outline_parts.get(layer, 0))
			"fairways":
				for row: Dictionary in rows:
					var line: PackedVector2Array = row["line"]
					if line.size() < 2:
						continue
					var st: Dictionary = STYLE.get(row["kind"], STYLE["biled"])
					var band := maxf(float(row["buffer_px"]) * 2.0, 1.0 / s)
					draw_polyline(line, st["band"], band)
					draw_polyline(line, st["line"], -1.0)
					emitted += 1
					drawn_kinds[row["kind"]] = int(drawn_kinds.get(row["kind"], 0)) + 1
			"constraint_points":
				for row: Dictionary in rows:
					_draw_point(row, s)
					emitted += 1
					drawn_kinds[row["kind"]] = int(drawn_kinds.get(row["kind"], 0)) + 1
		drawn[layer] = emitted
	draw_set_transform(Vector2.ZERO, 0.0, Vector2.ONE)


func _styles_of(layer: String) -> Array:
	if layer == "exclusions":
		return ["salmon_fjord", "protected_area", "coral_closure"]
	return [layer]


func _draw_point(row: Dictionary, s: float) -> void:
	var st: Dictionary = STYLE.get(row["kind"], STYLE["anadromous_river"])
	var c: Vector2 = row["px"]
	var r: float = row["radius_px"]
	if row["kind"] == "coral_reef":
		draw_circle(c, REEF_DOT_PX / s, st["fill"])
		return
	if (st["fill"] as Color).a > 0.0:
		draw_circle(c, r, st["fill"])
	if bool(row["hard"]):
		draw_arc(c, r, 0.0, TAU, 48, st["line"], -1.0)
	else:
		for i in DASH_SEGMENTS:
			var a := TAU * i / DASH_SEGMENTS
			draw_arc(c, r, a, a + TAU / DASH_SEGMENTS * 0.55, 4, st["line"], -1.0)
	draw_circle(c, 1.4 / s, st["line"])


## Merge each style's fills and outlines once per area.
func _build_batches() -> void:
	_fills = {}
	_outlines = {}
	_style_rows = {}
	_outline_parts = {}
	if _area.is_empty():
		return
	var layers: Dictionary = _area["layers"]
	for layer: String in ["exclusions", "locality_areas", "mooring_areas"]:
		var by_style := {}
		for row: Dictionary in layers.get(layer, []):
			var style: String = row["kind"] if layer == "exclusions" else layer
			if not STYLE.has(style):
				style = "protected_area"
			if not by_style.has(style):
				by_style[style] = []
			(by_style[style] as Array).append(row)
		for style: String in by_style:
			var points := PackedVector2Array()
			var indices := PackedInt32Array()
			var colors := PackedColorArray()
			var lines := PackedVector2Array()
			var colour: Color = STYLE[style]["fill"]
			var rows_out := 0
			for row: Dictionary in by_style[style]:
				var before := indices.size() + lines.size()
				for part: Dictionary in row["parts"]:
					var ring: PackedVector2Array = part["ring"]
					var tris: PackedInt32Array = part["tris"]
					var fill_points: PackedVector2Array = part.get("fill_points", ring)
					if tris.is_empty():
						_outline_parts[layer] = int(_outline_parts.get(layer, 0)) + 1
					elif colour.a > 0.0:
						var base := points.size()
						points.append_array(fill_points)
						for i in tris:
							indices.append(base + i)
						for i in fill_points.size():
							colors.append(colour)
					lines = _with_ring(lines, ring)
					for hole: PackedVector2Array in part["holes"]:
						lines = _with_ring(lines, hole)
				if indices.size() + lines.size() > before:
					rows_out += 1
			_fills[style] = {"points": points, "indices": indices, "colors": colors}
			_outlines[style] = lines
			_style_rows[style] = rows_out


## lines with the ring's closed outline appended as segment pairs.
static func _with_ring(lines: PackedVector2Array, ring: PackedVector2Array) -> PackedVector2Array:
	var n := ring.size()
	if n < 2:
		return lines
	for i in n:
		lines.append(ring[i])
		lines.append(ring[(i + 1) % n])
	return lines
