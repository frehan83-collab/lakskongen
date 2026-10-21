class_name GeoSiteLayer
extends Control
## The national map's aquaculture sites (geo stages 2-3 plan, Track C), from
## the sim's packed arrays (GeoBridgeAdapter.sites, Track A's get_geo_sites):
## type-coded icons (sea ring, orange land tank, buoy-yellow offshore
## 12-gon, small grey dots for other species), the ring in the owner's band
## (CompetitorFarmVisualManager.band_color_for for the majors, light slate
## for independents), clustered on a screen grid at national scale.
##
## The whole layer is one _draw, recorded again only when the view changes
## (resize), the sites change (a new rival layout signature) or the layer is
## toggled: never on a sim tick. redraw_count counts the recordings. Hover
## and the player's own farms are separate small canvas items (PlayerMarks).
##
## The cluster badges, and the screen grid that spaces them, grow by the
## view's symbol scale (GeoMapView.symbol_scale) on a window small enough
## that their numerals would draw under 11 screen pixels.

const CELL_PX := 16.0
const MIN_GAP_PX := 13.0
const ICON_HIT_PX := 6.0
const BADGE_FONT := 11
## A single site's icon reaches this far from its point (the offshore 12-gon).
const SINGLE_REACH_PX := 5.6
## The majors' arc ends this far outside a badge's disc (x the symbol scale):
## drawn at r + 2.6 with a width of 2.6 (_draw_badge).
const BADGE_ARC_OUT := 3.9
## Clear space kept between two marks (x the symbol scale).
const BADGE_PAD_PX := 1.0

## Times _draw recorded the layer (tests: unchanged over days at 30x).
var redraw_count: int = 0

var _sites: Dictionary = {}
var _classes := PackedByteArray()
var _owner_of := PackedInt32Array()
var _owner_major: Array[bool] = []
var _owner_band: Array[Color] = []
var _local := PackedVector2Array()
var _clusters: Array = []
var _origin := Vector2.ZERO
var _scale := 1.0
var _bounds: Array = []
var _res := 1.0
var _symbol := 1.0


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE


## The sites as get_geo_sites returns them.
func set_sites(sites: Dictionary) -> void:
	_sites = sites
	_classes = GeoBridgeAdapter.classes_of(sites)
	_owner_of = sites.get("owner", PackedInt32Array())
	_owner_major.clear()
	_owner_band.clear()
	for o: Variant in sites.get("owners", []):
		var row: Dictionary = o if o is Dictionary else {}
		var major := bool(row.get("is_major", false))
		_owner_major.append(major)
		_owner_band.append(CompetitorFarmVisualManager.band_color_for(str(row.get("id", ""))) if major
			else MapPalette.independent_band())
	_rebuild()


## Map national-image pixels to this layer: local = origin + px * scale;
## symbol_scale (>= 1) sizes the cluster badges and their grid.
func set_view(origin: Vector2, px_scale: float, bounds_25833: Array, res_m: float, symbol_scale: float = 1.0) -> void:
	_origin = origin
	_scale = px_scale
	_bounds = bounds_25833
	_res = res_m
	_symbol = maxf(1.0, symbol_scale)
	_rebuild()


## The badges' and the grid's scale (1 at 1080p and above).
func symbol_scale() -> float:
	return _symbol


## A cluster badge's radius as drawn on this layer: the scaled base radius,
## or more where its numeral needs it (a three-digit count when the clusters
## are coarse), so the numeral always sits on the dark disc.
func badge_radius_now(count: int) -> float:
	var r := badge_radius(count) * _symbol
	var font := get_theme_font(&"font", &"BodySmallStrong") if is_inside_tree() else null
	if font != null:
		var w := font.get_string_size(str(count), HORIZONTAL_ALIGNMENT_LEFT, -1, badge_font_size()).x
		r = maxf(r, w * 0.5 + 2.0 * _symbol)
	return r


## The badges' numeral size as drawn on this layer (layout pixels), rounded
## up so the scaled size never falls under the screen minimum.
func badge_font_size() -> int:
	return ceili(float(BADGE_FONT) * _symbol - 0.001)


## The clusters drawn (GeoClusterer's dictionaries).
func clusters() -> Array:
	return _clusters


## Every site's position on the layer, in site order.
func local_points() -> PackedVector2Array:
	return _local


## The sites as set (the packed arrays).
func sites() -> Dictionary:
	return _sites


## Is site i owned by a roster company (a major)?
func is_major_owner(owner_index: int) -> bool:
	return owner_index >= 0 and owner_index < _owner_major.size() and _owner_major[owner_index]


## Site i's owner index into owners (-1: none).
func owner_of(i: int) -> int:
	return _owner_of[i] if i >= 0 and i < _owner_of.size() else -1


## The band colour drawn for site i's owner.
func band_of_site(i: int) -> Color:
	var o := owner_of(i)
	return _owner_band[o] if o >= 0 and o < _owner_band.size() else MapPalette.independent_band()


## The cluster or single site under a layer point: {kind: "cluster"|"site",
## cluster: {...}, index: site index (singles), pos, radius}, or {}.
func item_at(p: Vector2) -> Dictionary:
	if not is_visible_in_tree():
		return {}
	var best: Dictionary = {}
	var best_d := INF
	for c: Dictionary in _clusters:
		var r := ICON_HIT_PX if int(c["count"]) == 1 else badge_radius_now(int(c["count"])) + 2.0
		var d := (c["pos"] as Vector2).distance_to(p)
		if d <= r and d < best_d:
			best_d = d
			if int(c["count"]) == 1:
				var i: int = (c["members"] as PackedInt32Array)[0]
				best = {"kind": "site", "cluster": c, "index": i, "pos": _local[i], "radius": ICON_HIT_PX}
			else:
				best = {"kind": "cluster", "cluster": c, "pos": c["pos"], "radius": r}
	return best


static func badge_radius(count: int) -> float:
	return clampf(5.0 + 1.6 * log(float(count)) / log(2.0), 6.5, 11.5)


## The colour of an icon class.
static func class_color(cls: int) -> Color:
	match cls:
		GeoBridgeAdapter.CLASS_SEA:
			return MapPalette.snow()
		GeoBridgeAdapter.CLASS_LAND:
			return MapPalette.land_orange()
		GeoBridgeAdapter.CLASS_OFFSHORE:
			return MapPalette.buoy()
	return MapPalette.other_species()


func _rebuild() -> void:
	var xs: PackedInt32Array = _sites.get("x", PackedInt32Array())
	var ys: PackedInt32Array = _sites.get("y", PackedInt32Array())
	_local.resize(xs.size())
	if _bounds.size() == 4:
		for i in xs.size():
			_local[i] = _origin + GeoProjection.to_px(float(xs[i]), float(ys[i]), _bounds, _res) * _scale
	_clusters = GeoClusterer.cluster(_local, _classes, _owner_of, CELL_PX * _symbol, MIN_GAP_PX * _symbol)
	# GEO-VIS: no badge drawn over another or over a single site (badges grown
	# for a small window stacked over each other and the city names).
	_clusters = GeoClusterer.declutter(_clusters, _local, _classes, _owner_of, gap_between)
	queue_redraw()


## How far a cluster of `count` sites draws from its centre: a single site's
## icon, or a badge with its halo and the majors' outer arc.
func reach_of(count: int) -> float:
	if count <= 1:
		return SINGLE_REACH_PX
	return badge_radius_now(count) + BADGE_ARC_OUT * _symbol


## The least distance between the centres of clusters of `a` and `b` sites
## that keeps either from covering the other: two badges keep their discs,
## halos and the majors' arcs apart; a single site's icon keeps off a badge's
## disc and halo, its numeral (an arc may graze it). Two single sites are
## never closer than the grid's gap, which is more.
func gap_between(a: int, b: int) -> float:
	var pad := BADGE_PAD_PX * _symbol
	if a > 1 and b > 1:
		return reach_of(a) + reach_of(b) + pad
	if a > 1 or b > 1:
		return badge_radius_now(maxi(a, b)) + 1.0 * _symbol + SINGLE_REACH_PX + pad
	return SINGLE_REACH_PX * 2.0 + pad


func _draw() -> void:
	redraw_count += 1
	var font := get_theme_font(&"font", &"BodySmallStrong")
	var deep := MapPalette.deep_water()
	# Quiet first: other species, then singles, then the cluster badges.
	for pass_index in 3:
		for c: Dictionary in _clusters:
			var n: int = c["count"]
			if n == 1:
				var i: int = (c["members"] as PackedInt32Array)[0]
				var cls := _classes[i]
				if (pass_index == 0) == (cls == GeoBridgeAdapter.CLASS_OTHER) and pass_index < 2:
					draw_site_icon(self, _local[i], cls, band_of_site(i), is_major_owner(owner_of(i)))
			elif pass_index == 2:
				_draw_badge(c, font, deep)


## One site's icon: the kind by shape, the owner by the ring's band.
static func draw_site_icon(canvas: CanvasItem, p: Vector2, cls: int, band: Color, major: bool) -> void:
	var deep := MapPalette.deep_water()
	match cls:
		GeoBridgeAdapter.CLASS_SEA:
			canvas.draw_circle(p, 4.2, Color(deep, 0.7))
			canvas.draw_arc(p, 2.8, 0.0, TAU, 14, band if major else MapPalette.snow(), 1.7, true)
		GeoBridgeAdapter.CLASS_LAND:
			canvas.draw_rect(Rect2(p - Vector2(3.6, 3.6), Vector2(7.2, 7.2)), Color(deep, 0.8))
			canvas.draw_rect(Rect2(p - Vector2(2.6, 2.6), Vector2(5.2, 5.2)), MapPalette.land_orange())
			if major:
				canvas.draw_rect(Rect2(p - Vector2(3.6, 3.6), Vector2(7.2, 7.2)), band, false, 1.2)
		GeoBridgeAdapter.CLASS_OFFSHORE:
			canvas.draw_colored_polygon(_ngon(p, 5.6, 12), Color(deep, 0.85))
			canvas.draw_colored_polygon(_ngon(p, 4.4, 12), MapPalette.buoy())
			if major:
				canvas.draw_polyline(_closed(_ngon(p, 5.6, 12)), band, 1.2, true)
		_:
			canvas.draw_circle(p, 1.7, Color(MapPalette.other_species(), 0.85))


func _draw_badge(c: Dictionary, font: Font, deep: Color) -> void:
	var p: Vector2 = c["pos"]
	var n: int = c["count"]
	var r := badge_radius_now(n)
	var k := _symbol
	var cls := GeoClusterer.dominant_class(c)
	# The majors' share: an outer arc in each major's band, sized by its sites.
	var start := -PI * 0.5
	var owners: Dictionary = c["owners"]
	var keys := owners.keys()
	keys.sort()
	for o: int in keys:
		if not is_major_owner(o):
			continue
		var sweep := TAU * float(owners[o]) / float(n)
		draw_arc(p, r + 2.6 * k, start, start + sweep, maxi(4, int(sweep * 8.0)), _owner_band[o], 2.6 * k, true)
		start += sweep
	draw_circle(p, r + 1.0 * k, Color(deep, 0.55))
	draw_circle(p, r, Color(deep, 0.92))
	draw_arc(p, r - 1.0 * k, 0.0, TAU, 32, class_color(cls), 2.0 * k, true)
	if font != null:
		var text := str(n)
		var size_px := badge_font_size()
		var sz := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, size_px)
		draw_string(font, p + Vector2(-sz.x * 0.5, font.get_ascent(size_px) * 0.5 - 1.0 * k), text,
			HORIZONTAL_ALIGNMENT_LEFT, -1, size_px, MapPalette.snow())


static func _ngon(c: Vector2, r: float, n: int) -> PackedVector2Array:
	var out := PackedVector2Array()
	for k in n:
		var a := TAU * float(k) / float(n) - PI * 0.5
		out.append(c + Vector2(cos(a), sin(a)) * r)
	return out


static func _closed(poly: PackedVector2Array) -> PackedVector2Array:
	var out := poly.duplicate()
	out.append(poly[0])
	return out


## The company's own farms (get_player_site_locations): salmon discs with a
## light ring, above every other icon. A small canvas item of its own, so a
## new farm re-records only these few marks.
class PlayerMarks extends Control:
	## A mark's disc: the hover ring and the pointer's reach.
	const RADIUS := 8.5
	const HIT_PX := 8.0
	var redraw_count: int = 0
	var _rows: Array = []
	var _points := PackedVector2Array()

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE

	## rows from get_player_site_locations, positions on this layer.
	func set_marks(rows: Array, points: PackedVector2Array) -> void:
		_rows = rows
		_points = points
		queue_redraw()

	func rows() -> Array:
		return _rows

	func points() -> PackedVector2Array:
		return _points

	## The row under a point, or {}: the nearest mark within HIT_PX (on
	## equal distance the one drawn later, above).
	func row_at(p: Vector2) -> Dictionary:
		var i := index_at(p)
		return _rows[i] if i >= 0 else {}

	## The index of the mark under a point, or -1.
	func index_at(p: Vector2) -> int:
		var best := -1
		var best_d := INF
		for i in _points.size():
			var d := _points[i].distance_to(p)
			if d <= HIT_PX and d <= best_d:
				best_d = d
				best = i
		return best

	func _draw() -> void:
		redraw_count += 1
		var deep := MapPalette.deep_water()
		for p in _points:
			draw_circle(p, RADIUS, Color(deep, 0.75))
			draw_circle(p, 7.0, MapPalette.snow())
			draw_circle(p, 5.2, MapPalette.salmon())
			draw_circle(p, 1.8, deep)
